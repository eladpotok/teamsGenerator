import { SettingOutlined } from "@ant-design/icons";
import { Button, Col, Drawer, Row, Select } from "antd";
import Config from "../Configuration/Config";
import { useState } from "react";

function MobileHeader(props) {

    const [configDrawerOpen, setConfigDrawerOpen] = useState(false)
    
    const algosItems = props.storeConfig.algos.map(algo => {
        return {
            label: algo.displayName,
            value: algo.algoKey.toString()
        }
    })

    function openCloseConfigDrawerHandler(isOpen) {
        setConfigDrawerOpen(isOpen)
    }

    function selectChangedHandler(value) {
        var selectedAlgo = props.storeConfig.algos.filter(algo => algo.algoKey == value)[0]
        props.onAlgoChanged(selectedAlgo)
    };

    return (<Row style={{ marginTop: '4px' }}>
        <Col flex='none'>
            <Button style={{ margin: '4px'   }} size="large" onClick={() => { openCloseConfigDrawerHandler(true) }} icon={<SettingOutlined />} />
        </Col>
        <Col flex="auto">
            {algosItems && <div>
                <Select size="large" style={{ display: 'flex', margin: '4px' }}
                    onChange={selectChangedHandler}
                    defaultValue={props.defaultAlgoName}
                    options={algosItems} />
            </div>}
        </Col>

        <Drawer style={{ backgroundColor: 'white' }}
                title='User Configuration'
                width='85%'
                onClose={() => { openCloseConfigDrawerHandler(false) }}
                open={configDrawerOpen}>
                {props.storeConfig && <Config backgroundColor='white' currentAlgo={props.storeConfig.algos.filter(t => t.algoKey == 0)[0]} shirtsColors={props.storeConfig.config.shirtsColors} numberOfTeams={props.storeConfig.config.numberOfTeams} />}
        </Drawer>

    </Row>)
}

export default MobileHeader;