import { SettingOutlined, WarningOutlined } from "@ant-design/icons";
import { Button, Col, Drawer, Modal, Row, Select } from "antd";
import Config from "../Configuration/Config";
import { useState } from "react";
import AreYouSureModal from "../Common/AreYouSureModal";


function MobileHeader(props) {

    const [configDrawerOpen, setConfigDrawerOpen] = useState(false)
    const [areYouSureOpen, setAreYouSureOpen] = useState(false)
    
    const [areYouSureContext, setAreYouSureContext] = useState({})
    


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
        if(props.userConfig.algo.algoKey != value && props.players && props.players.length > 0) {
            setAreYouSureContext(value)
            setAreYouSureOpen(true)   
        }
        else {
            changeAlgo(value)
        }
        
    };

    function areYouSureYesClickedHandler(context){
        changeAlgo(context) 
        setAreYouSureOpen(false)
        props.onClearPlayers()
    }

    function areYouSureNoClickedHandler(){
        changeAlgo(props.userConfig.algo.algoKey)
        setAreYouSureOpen(false)
    }

    function changeAlgo(value){
        var selectedAlgo = props.storeConfig.algos.filter(algo => algo.algoKey == value)[0]
        props.onAlgoChanged(selectedAlgo)
    }

    return (<Row style={{ marginTop: '4px' }}>
        <Col flex='none'>
            <Button style={{ margin: '4px'   }} size="large" onClick={() => { openCloseConfigDrawerHandler(true) }} icon={<SettingOutlined />} />
        </Col>
        <Col flex='auto' >
            {algosItems && <div>
                <Select size="large" style={{ display: 'flex', margin: '4px' }}
                        onChange={selectChangedHandler} 
                        onMouseDown={()=>{console.log('mouse down')}}
                        value={props.defaultAlgoName}
                        options={algosItems} />
            </div>}
        </Col>
        {/* <label style={{overflow: 'hidden', color: 'red', fontStyle: 'italic', margin: '4px'}}><WarningOutlined style={{marginRight: '4px'}} />Algorithms selection Cann't be modified since there are at least one player.</label> */}

        <Drawer style={{ backgroundColor: 'white' }}
                title='User Configuration'
                width='85%'
                onClose={() => { openCloseConfigDrawerHandler(false) }}
                open={configDrawerOpen}>
                {props.storeConfig && <Config backgroundColor='white' currentAlgo={props.storeConfig.algos.filter(t => t.algoKey == 0)[0]} shirtsColors={props.storeConfig.config.shirtsColors} numberOfTeams={props.storeConfig.config.numberOfTeams} />}
        </Drawer>

        <AreYouSureModal description='This action will clear your players list since its not suitable with the new algorithm selection. Do you want to proceed?' title='Are you sure you want to change the algorithm?' context={areYouSureContext} show={areYouSureOpen} onNoClicked={areYouSureNoClickedHandler} onYesClicked={areYouSureYesClickedHandler} />

    </Row>)
}

export default MobileHeader;