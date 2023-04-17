import { ClearOutlined, ExportOutlined, MoreOutlined, NumberOutlined, UploadOutlined, UserAddOutlined } from "@ant-design/icons";
import { Button, Col, Drawer, Dropdown, Row } from "antd";
import { useState } from "react";
import ImportPlayer from "../../Common/ImportPlayer";
import { writeFileHandler } from "../../../Utilities/Helpers";
import AddPlayerForm from "../../Common/AddPlayerForm";

function MobilePlayersListPanel(props) {
    const [addPlayerDrawerOpen, setAddPlayerDrawerOpen] = useState(false)

    const items = [
        {
            label: <ImportPlayer>
                Import
            </ImportPlayer>,
            key: '1',
            icon: <UploadOutlined />,
        },
        {
            label: <div onClick={() => { writeFileHandler(props.players) }}>Export</div>,
            key: '2',
            icon: <ExportOutlined />,
        },
        {
            label: <div onClick={props.onClearPlayers}>Clear Players</div>,
            key: '3',
            icon: <ClearOutlined/>,
        }
    ];


    return (
        <Row style={{ backgroundColor: '#4a4a4a', borderRadius: '10px', marginLeft: '4px', marginRight: '4px' }}>
            <Col flex='auto'>
                <div style={{ display: 'flex', color: 'white', margin: '10px' }}><NumberOutlined style={{margin: '3px'}} size='small' />Total: {props.players.length}</div>
            </Col>
            <Col flex='none' >
                <Button  onClick={()=>{setAddPlayerDrawerOpen(true)}} shape="square" icon={<UserAddOutlined  style={{ color: 'white' }} />} style={{ margin: '4px', 'background-color': 'transparent', borderWidth: '1px' }} />
                <Dropdown menu={{ items }} placement="bottomLeft" arrow>
                    <Button style={{ margin: '4px', 'background-color': 'transparent', borderWidth: '1px'}} icon={<MoreOutlined style={{ color: 'white' }} />}></Button>
                </Dropdown>
            </Col>

            {props.currentAlgo && <Drawer title="Add New Player" 
                        width='75%'
                        onClose={() => { setAddPlayerDrawerOpen(false) }}
                        open={addPlayerDrawerOpen}>
                    <AddPlayerForm players={null} playersProperties={props.currentAlgo.playerProperties}/>
                </Drawer>}
        </Row>
    )
}

export default MobilePlayersListPanel;