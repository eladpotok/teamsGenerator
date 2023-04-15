import { Button, Col, Drawer, Dropdown, FloatButton, Row, Select, MenuProps, message} from "antd";
import { useContext, useState } from "react";
import { PlayersContext } from "../../../Store/PlayersContext";
import MobilePlayersList from "./MobilePlayersList";
import AddPlayerForm from "../../Common/AddPlayerForm";
import { ClearOutlined, EllipsisOutlined, ExportOutlined, MoreOutlined, UploadOutlined, UserAddOutlined } from "@ant-design/icons";
import Files from 'react-files'
import ImportPlayer from "../../Common/ImportPlayer";
import { writeFileHandler } from "../../../Utilities/Helpers";

function MobilePlayersMenu(props) {

    const playersContext = useContext(PlayersContext)
    const [messageApi, contextHolder] = message.useMessage();

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
          label: <div onClick={()=>{writeFileHandler(playersContext.players)}}>Export</div>,
          key: '2',
          icon: <ExportOutlined />,
        },
        {
            label: <div onClick={()=>{playersContext.setPlayers([])}}>Clear Players</div>,
            key: '3',
            icon: <ClearOutlined />,
          }
      ];

    function openCloseAddPlayerDrawer(isOpen) {
        setAddPlayerDrawerOpen(isOpen)
    }

    function playerRemovedHandler(playerToRemove){
        const updatedPlayers = playersContext.players.filter(player => player.key != playerToRemove.key)
        playersContext.setPlayers(updatedPlayers)
    }

    return (
        <div>
            {contextHolder}
            {playersContext.players && <div>
                <Row >
                    <Col flex='auto'>
                        <div style={{ display: 'flex', color: 'white', margin: '10px' }}>Total: {playersContext.players.length}</div>
                    </Col>

                    <Col flex='none'>
                        <Button onClick={()=>{openCloseAddPlayerDrawer(true)}} shape="square" icon={<UserAddOutlined />} style={{margin: '4px'}} />
                        <Dropdown  menu={{ items }} placement="bottomLeft" arrow>
                            <Button style={{margin: '4px'}} icon={<MoreOutlined />}></Button>
                        </Dropdown>
                    </Col>
                </Row>

                <Row>
                    <Col flex='auto'>
                        <MobilePlayersList  onPlayerRemoved={playerRemovedHandler} playerProperties={props.currentAlgo.playerProperties} players={playersContext.players}/>
                    </Col>
                </Row>

                {props.currentAlgo && <Drawer title="Add New Player" style={{backgroundColor: '#4b525e'}}
                        width='75%'
                        onClose={() => { openCloseAddPlayerDrawer(false) }}
                        open={addPlayerDrawerOpen}>
                    <AddPlayerForm playersProperties={props.currentAlgo.playerProperties}/>
                </Drawer>}

            </div>}
        </div>
    )

}

export default MobilePlayersMenu;